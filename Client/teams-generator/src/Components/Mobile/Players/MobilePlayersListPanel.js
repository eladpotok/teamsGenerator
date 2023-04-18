import { CheckSquareOutlined, ClearOutlined, ExportOutlined, FileDoneOutlined, MoreOutlined, NumberOutlined, UploadOutlined, UserAddOutlined } from "@ant-design/icons";
import { Button, Col, Drawer, Dropdown, Row } from "antd";
import { useState } from "react";
import ImportPlayer from "../../Common/ImportPlayer";
import { writeFileHandler } from "../../../Utilities/Helpers";
import AddPlayerForm from "../../Common/AddPlayerForm";

function MobilePlayersListPanel(props) {
    const [addPlayerDrawerOpen, setAddPlayerDrawerOpen] = useState(false)

    const items = [
        {
            label: <div onClick={()=>{props.onMarkAllPlayers(true)}}>Mark All</div>,
            key: '1',
            icon: <FileDoneOutlined />
        },
        {
            label: <div onClick={()=>{props.onMarkAllPlayers(false)}}>Unmark All</div>,
            key: '2',
            icon: <FileDoneOutlined />
        },
        {
            label: <ImportPlayer>
                Import
            </ImportPlayer>,
            key: '3',
            icon: <UploadOutlined />,
        },
        {
            label: <div onClick={() => { writeFileHandler(props.players) }}>Export</div>,
            key: '4',
            icon: <ExportOutlined />,
        },
        {
            label: <div onClick={props.onClearPlayers}>Clear Players</div>,
            key: '5',
            icon: <ClearOutlined/>,
        }
    ];


    return (
        <Row style={{ backgroundColor: '#4a4a4a', borderRadius: '10px', marginLeft: '4px', marginRight: '4px' }}>
            <Col flex='auto'>
                <div style={{ display: 'flex', color: 'white', margin: '10px' }}><NumberOutlined style={{margin: '3px'}} size='small' />Total: {props.players.length} <CheckSquareOutlined size='small' style={{marginLeft: '13px', marginTop:'3px', marginRight: '3px'}} /> Arrive: {props.arrivedPlayers.length}</div>
            </Col>
            <Col flex='none' >
                <Button  onClick={()=>{setAddPlayerDrawerOpen(true)}} shape="square" icon={<UserAddOutlined  style={{ color: 'white'  }} />} style={{ margin: '4px', 'background-color': 'transparent', borderWidth: '1px' ,colorPrimaryBorder: 'white', colorPrimaryActive:'white',colorPrimaryHover:'white',colorBorder:'white', }} />
                <Dropdown menu={{ items }} placement="bottomLeft" arrow>
                    <Button colorPrimaryBorder='white' colorBgContainer='white' colorPrimaryHover='white' colorBorder='white' style={{ margin: '4px', 'background-color': 'transparent', borderWidth: '1px',colorPrimaryBorder: 'white', colorBgContainer:'white',colorPrimaryHover:'white',colorBorder:'white'}} icon={<MoreOutlined style={{ color: 'white',colorBorder: 'white' }} />}></Button>
                </Dropdown>
            </Col>

            {props.currentAlgo && <Drawer title="Add New Player" 
                        width='75%'
                        onClose={() => { setAddPlayerDrawerOpen(false) }}
                        open={addPlayerDrawerOpen}>
                    <AddPlayerForm players={null} playersProperties={props.currentAlgo.playerProperties}/>
                </Drawer>}
        </Row>
    )
}

export default MobilePlayersListPanel;