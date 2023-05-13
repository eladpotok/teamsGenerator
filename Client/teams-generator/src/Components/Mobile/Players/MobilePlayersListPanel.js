import { CheckSquareOutlined, ClearOutlined, ExportOutlined, FileDoneOutlined, MoreOutlined, NumberOutlined, UploadOutlined, UserAddOutlined } from "@ant-design/icons";
import { Button, Col, Drawer, Dropdown, Modal, Row, message } from "antd";
import { useContext, useState } from "react";
import ImportPlayer from "../../Common/ImportPlayer";
import { writeFileHandler } from "../../../Utilities/Helpers";
import AddPlayerForm from "../../Common/AddPlayerForm";
import { AnalyticsContext } from "../../../Store/AnalyticsContext";
import { UserContext } from "../../../Store/UserContext";
import { PlayersContext } from "../../../Store/PlayersContext";

function MobilePlayersListPanel(props) {
    const [addPlayerDrawerOpen, setAddPlayerDrawerOpen] = useState(false)
    const analyticsContext = useContext(AnalyticsContext)
    const userContext = useContext(UserContext)
    const playersContext = useContext(PlayersContext)

    const [playerToAdd, setPlayerToAdd] = useState({})
    const [messageApi, contextHolder] = message.useMessage();


    const items = [
        {
            label: <div  onClick={()=>{props.onMarkAllPlayers(true)}}>Mark All</div>,
            key: '1',
            icon: <FileDoneOutlined />
        },
        {
            label: <div onClick={()=>{props.onMarkAllPlayers(false)}}>Unmark All</div>,
            key: '2',
            icon: <FileDoneOutlined />
        },
        {
            label: <ImportPlayer algos={props.algos}>
                Import
            </ImportPlayer>,
            key: '3',
            icon: <UploadOutlined />,
        },
        {
            label: <div onClick={() => { writeFileHandler(props.players, props.currentAlgo); analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'exportPlayers', null)
        }}>Export</div>,
            key: '4',
            icon: <ExportOutlined />,
        },
        {
            label: <div onClick={props.onClearPlayers}>Clear Players</div>,
            key: '5',
            icon: <ClearOutlined/>,
        }
    ];

    function openAddPlayerViewHandler() {
        setPlayerToAdd(getDefaultValues(props.currentAlgo.playerProperties));
        setAddPlayerDrawerOpen(true)
    }

    function resetClickedHandler() {
        setPlayerToAdd(getDefaultValues(props.currentAlgo.playerProperties));
    }

    function playerSubmittedHandler() {
        analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'addPlayer', playerToAdd)
        playersContext.setPlayers([...playersContext.players, playerToAdd])
        setPlayerToAdd(getDefaultValues(props.currentAlgo.playerProperties));

        messageApi.open({
            type: 'success',
            content: 'Player Added',
          });
    }


    return (
        <Row style={{ backgroundColor: '#4a4a4a', borderRadius: '10px', marginLeft: '4px', marginRight: '4px' }}>
            {contextHolder}
            <Col flex='auto'>
                <div style={{ display: 'flex', color: 'white', margin: '10px' }}><NumberOutlined style={{margin: '3px'}} size='small' />Total: {props.players.length} <CheckSquareOutlined size='small' style={{marginLeft: '13px', marginTop:'3px', marginRight: '3px'}} /> Arrive: {props.arrivedPlayers.length}</div>
            </Col>
            <Col flex='none' >
                <Button  onClick={openAddPlayerViewHandler} shape="square" icon={<UserAddOutlined  style={{ color: 'white'  }} />} style={{ margin: '4px', 'background-color': 'transparent', borderWidth: '1px' ,colorPrimaryBorder: 'white', colorPrimaryActive:'white',colorPrimaryHover:'white',colorBorder:'white', }} />
                <Dropdown menu={{ items }} placement="bottomLeft" arrow>
                    <Button colorPrimaryBorder='white' colorBgContainer='white' colorPrimaryHover='white' colorBorder='white' style={{ margin: '4px', 'background-color': 'transparent', borderWidth: '1px',colorPrimaryBorder: 'white', colorBgContainer:'white',colorPrimaryHover:'white',colorBorder:'white'}} icon={<MoreOutlined style={{ color: 'white',colorBorder: 'white' }} />}></Button>
                </Dropdown>
            </Col>

            {props.currentAlgo && <Modal title={<label style={{marginLeft: '4px', color: '#095c1f'}}>NEW PLAYER</label>}  style={{background: "rgb(46, 46, 46)"}} footer={[]}
                        width='75%'
                        onCancel={() => { setAddPlayerDrawerOpen(false) }}
                        open={addPlayerDrawerOpen}>
                    <AddPlayerForm player={playerToAdd} playersProperties={props.currentAlgo.playerProperties} onResetClicked={resetClickedHandler} onPlayerSubmitted={playerSubmittedHandler}/>
                </Modal>}
        </Row>
    )
}

function getDefaultValues(properties) {
    let playerDefaultValues = {}
    properties.forEach(element => {
        if (element.type == 'number') {
            playerDefaultValues[element.name] = 4
        }
        if (element.type == 'text') {
            console.log('test', element.name, playerDefaultValues)
            playerDefaultValues[element.name] = ""
        }

    });

    return playerDefaultValues
}

export default MobilePlayersListPanel;