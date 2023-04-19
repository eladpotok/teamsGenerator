import { useContext, useEffect, useState } from "react";
import { Button, Form, Input, Popconfirm, Table, Modal, Card,  Descriptions, Divider } from 'antd';
import { UploadOutlined, UserAddOutlined, QuestionCircleOutlined, ExportOutlined } from '@ant-design/icons';
import './PlayersTable.css';
import React from 'react';
import { PlayersContext } from "../Store/PlayersContext";
import { v4 as uuidv4 } from 'uuid';
import { ConfigurationStoreContext } from "../Store/ConfigurationStoreContext";
import ImportPlayer from "./Common/ImportPlayer";
import { writeFileHandler } from '../Utilities/Helpers'


function PlayersTable(props) {
    
    const [columns, setColumns] = useState(null)
    const [openNewPlayerModal, setOpenNewPlayerModal] = useState(false)
    const [selectedPlayersKey, setSelectedPlayersKey] = useState([]);


    const playersContext = useContext(PlayersContext)
    const storeConfigContext = useContext(ConfigurationStoreContext);
    
    const playersProperties = storeConfigContext.storeConfig.algos[props.algoKey].playerProperties
    
    useEffect(() => {
        (async () => {
            if (!columns) {
                generateTableColumns(storeConfigContext.storeConfig.algos[props.algoKey].playerProperties);
            }
        })()
    }, [columns])


    function addPlayerHandler(player) {
        playersContext.setPlayers([...playersContext.players, player])
    }

    function removePlayerHandler() {
        const updatedPlayers = playersContext.players.filter(player => !selectedPlayersKey.includes(player.key))
        playersContext.setPlayers(updatedPlayers)
        setSelectedPlayersKey([])
    }


    function generateTableColumns(playersProperties) {
        const columns = playersProperties.filter(f => f.showInClient).map((property) => {
            return {
                title: property.name,
                dataIndex: property.name
            }
        })
        setColumns(columns)
    }

    const formFinishedHandler = (values) => {
        values['key'] = uuidv4();
        addPlayerHandler(values)
        resetFromHandler();
    };

    const layout = {
        labelCol: { span: 4 },
        wrapperCol: { span: 16 },
    };
    const resetFromHandler = () => {
        form.resetFields();
    };

    const modalCancelHandler = () => {
        setOpenNewPlayerModal(false);
        resetFromHandler();
    };



 
 
 
    function clearPlayersHandler() {
        playersContext.setPlayers([])
    }



    const onSelectChange = (newSelectedPlayers) => {
        setSelectedPlayersKey(newSelectedPlayers)
    };


    const rowSelection = {
        hideSelectAll: true,
        selectedPlayersKey: selectedPlayersKey,
        onChange: onSelectChange,
    };

    const [form] = Form.useForm();

    
    return (
        <Card >
            <Modal onCancel={modalCancelHandler} open={openNewPlayerModal} footer={[]} title='Add Player'>
                <Form {...layout} form={form} onFinish={formFinishedHandler} style={{ marginTop: '15px' }}>

                    {playersProperties && playersProperties.filter(f => f.showInClient).map((property) => 

                        <Form.Item name={property.name} label={property.name} rules={[{ required: true }]}>
                            <Input type={property.type} tep={0.1} max={10} min={1} />
                        </Form.Item>

                    )}

                    <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>
                        <Button type="primary" htmlType="submit" style={{ marginRight: 16 }}>
                            Add
                        </Button>

                        <Button htmlType="button" onClick={resetFromHandler}>
                            Reset
                        </Button>
                    </div>

                </Form>
            </Modal>

            
            <div style={{ background: 'white', display: 'flex',  'flex-direction': 'row', 'align-items': 'center', 'justifyContent': 'flex-end' }}>

                <Descriptions style={{verticalAlignment: 'center', marginTop: '4px'}}>
                    <Descriptions.Item  label='Total Players'>{playersContext.players.length}</Descriptions.Item>
                </Descriptions>

                <Popconfirm disabled={playersContext.players.length === 0} title="Clear all Players" onConfirm={() => { clearPlayersHandler() }}
                            description={"Are you sure you want to clear all players?"}
                            icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                    <Button disabled={playersContext.players.length === 0} type='primary' danger  style={{ borderRadius: '5px',  marginRight: '5px' }}>
                        Clear All
                    </Button>                
                </Popconfirm>

                <Popconfirm disabled={selectedPlayersKey.length === 0} title="Remove selected Players" onConfirm={() => { removePlayerHandler() }}
                            description={"Are you sure you want to remove the selected players?"}
                            icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                    <Button disabled={selectedPlayersKey.length === 0} type='primary' danger  style={{ borderRadius: '5px', marginRight: '5px' }}>
                        Remove Player(s)
                    </Button>             
                </Popconfirm>

                <Button icon={<UserAddOutlined />} onClick={() => { setOpenNewPlayerModal(true) }} type="primary" style={{ borderRadius: '5px',  marginRight: '5px' }}>
                    Add
                </Button>

                <ImportPlayer>
                    <Button icon={<UploadOutlined />} type='primary' style={{ borderRadius: '5px', marginRight: '5px' }}>
                        Import
                    </Button>
                </ImportPlayer>

                <Button onClick={()=>{writeFileHandler(playersContext.players)}} icon={<ExportOutlined />} type='primary' style={{ borderRadius: '5px', marginRight: '5px' }}>
                        Export
                </Button>


            </div>
            
            <Divider/>

                <Table  rowSelection={rowSelection} dataSource={playersContext.players} columns={columns} />

            <Divider/>

        </Card>
    );

}

export default PlayersTable;