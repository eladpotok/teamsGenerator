import { useContext, useEffect, useState } from "react";
import { Button, Form, Input, Popconfirm, Table, Slider, Modal, Upload, Card, Space, Descriptions, Divider, message } from 'antd';
import { InfoCircleOutlined, UserOutlined, UploadOutlined, UserAddOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { getPlayersPropertiesByAlgo, getTeams } from '../Adapters/DB/WebApiAdapter'
import './PlayersTable.css';
import React from 'react';
import Files from 'react-files'
import { TeamsContext } from "../Store/TeamsContext";
import MyCard from "./UI/MyCard";
import { ConfigurationContext } from "../Store/ConfigurationContext";
import { PlayersContext } from "../Store/PlayersContext";


function PlayersTable(props) {

    const [playerProperties, setPlayerProperties] = useState(null)
    const [columns, setColumns] = useState([])
    const [addedPlayer, setAddedPlayer] = useState({})
    const [openNewPlayerModal, setOpenNewPlayerModal] = useState(false)
    const [messageApi, contextHolder] = message.useMessage();
    const key = 'updatable';

    const playerIndex = 1
    const teamsContext = useContext(TeamsContext);
    const configContext = useContext(ConfigurationContext);
    const playersContext = useContext(PlayersContext)

    useEffect(() => {
        (async () => {
            if (!playerProperties) {
                let properties = await getPlayersPropertiesByAlgo(props.algoKey);
                setPlayerProperties(properties);
                generateTableColumns(properties);
            }
        })()
    }, [playerProperties])


    function addPlayerHandler(player) {
        playersContext.setPlayers([...playersContext.players, player])
    }

    function removePlayerHandler() {
        const updatedPlayers = playersContext.players.filter(player => !selectedPlayersKey.includes(player.key))
        playersContext.setPlayers(updatedPlayers)
    }


    function generateTableColumns(playersProperties) {
        const columns = playersProperties.map((property) => {
            return {
                title: property.name,
                dataIndex: property.name
            }
        })
        setColumns(columns)
    }

    const onFinish = (values) => {
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

    const closeModalHandler = () => {
        setOpenNewPlayerModal(false);
        resetFromHandler();
    };

    const fileBrowserHandler = (files) => {
        const fileReader = new FileReader();

        fileReader.onload = e => {
            playersContext.setPlayers(JSON.parse(e.target.result))
            messageApi.open({
                key,
                type: 'success',
                content: 'Loaded!',
                duration: 2,
              })
        };
        messageApi.open({
            key,
            type: 'loading',
            content: 'Loading...',
          })
        fileReader.readAsText(files[0], "UTF-8");
    }

    const handleError = (error, file) => {
        console.log('error code ' + error.code + ': ' + error.message)
    }

    function clearPlayersHandler() {
        playersContext.setPlayers([])
    }

    async function generateTeamsHandler() {

        const selectedConfig  =configContext.getSelectedConfig();
        if (selectedConfig.shirtsColors.length < 3) {
            messageApi.open({
                type: 'error',
                content: 'Must choose 3 shirts colors at least',
              });
            return;
        }
        
        props.onGenerateClicked(props.algoKey)

        // const getTeamsResponse = await getTeams(props.algoKey, players, configContext.getSelectedConfig())
        // teamsContext.setTeams(getTeamsResponse.teams)
        // teamsContext.setResultAsText(getTeamsResponse.teamsResultAsCopyText)
    }

    const [selectedPlayersKey, setSelectedPlayersKey] = useState([]);

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
            {contextHolder}
            <Modal onCancel={closeModalHandler} open={openNewPlayerModal} footer={[]} title='Add Player'>
                <Form {...layout} form={form} onFinish={onFinish} style={{ marginTop: '15px' }}>

                    {playerProperties && playerProperties.map((property) => {

                        return <Form.Item name={property.name} label={property.name} rules={[{ required: true }]}>
                            <Input type={property.type} tep={0.1} max={10} min={1} />
                        </Form.Item>

                    })}

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

                <Popconfirm title="Clear all Players" onConfirm={() => { clearPlayersHandler() }}
                            description={"Are you sure you want to clear all players?"}
                            icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                    <Button disabled={playersContext.players.length == 0} type='primary' danger  style={{ borderRadius: '5px',  marginRight: '5px' }}>
                        Clear All
                    </Button>                
                </Popconfirm>

                <Popconfirm title="Remove selected Players" onConfirm={() => { removePlayerHandler() }}
                            description={"Are you sure you want to remove the selected players?"}
                            icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                    <Button disabled={selectedPlayersKey.length == 0} type='primary' danger  style={{ borderRadius: '5px', marginRight: '5px' }}>
                        Remove Player(s)
                    </Button>             
                </Popconfirm>

                <Button icon={<UserAddOutlined />} onClick={() => { setOpenNewPlayerModal(true) }} type="primary" style={{ borderRadius: '5px',  marginRight: '5px' }}>
                    Add
                </Button>

                <Files
                    className='files-dropzone'
                    onChange={fileBrowserHandler}
                    onError={handleError}
                    accepts={['.json']}
                    maxFileSize={10000000}
                    minFileSize={0}
                    clickable>
                    <Button icon={<UploadOutlined />} type='primary' style={{ borderRadius: '5px', marginRight: '5px' }}>
                        Import
                    </Button>
                </Files>

            </div>
            
            <Divider/>

            <Table  rowSelection={rowSelection} dataSource={playersContext.players} columns={columns} />

            <Divider/>
     






        </Card>
    );

}

export default PlayersTable;